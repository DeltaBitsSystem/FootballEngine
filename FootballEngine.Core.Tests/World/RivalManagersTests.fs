namespace FootballEngine.Core.Tests.World

open System
open Expecto
open Microsoft.FSharp.Reflection
open FootballEngine
open FootballEngine.Domain
open FootballEngine.Generation
open FootballEngine.Core.Match
open FootballEngine.World
open FootballEngine.World.Agents
open FootballEngine.World.Phases
open FootballEngine.Core.Tests.Infrastructure

/// Rival managers outside the match: who they are, what they study, the plan they prepare, what they remember and
/// how a good one lifts a background match.
module RivalManagersTests =

    let private cfg = BalanceConfig.defaultConfig.RivalManager

    /// The underdog rule stays out of the way of tests about the other approaches.
    let private noUnderdog = { cfg with UnderdogGap = 1.0e9 }

    let private baseState = lazy (WorldGenerationTests.generate 0xC02UL [])

    type private Scenario =
        { State: GameState
          FixtureId: MatchId
          Fixture: MatchFixture
          Rival: ClubId }

    let private scenario () : Scenario =
        let gs = baseState.Value

        let id, fixture =
            gs.Competitions
            |> Map.toSeq
            |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq)
            |> Seq.filter (fun (_, f) -> MatchFixture.isPending f && MatchFixture.involves gs.UserClubId f)
            |> Seq.sortBy (fun (id, f) -> f.ScheduledDate, id)
            |> Seq.head

        { State = { gs with CurrentDate = fixture.ScheduledDate.Date }
          FixtureId = id
          Fixture = fixture
          Rival = if fixture.HomeClubId = gs.UserClubId then fixture.AwayClubId else fixture.HomeClubId }

    let private userCoachId (s: Scenario) : StaffId =
        (GameState.headCoach s.State.UserClubId s.State).Value.Id

    let private rivalCoach (s: Scenario) : Staff = (GameState.headCoach s.Rival s.State).Value

    let private editRival (f: Staff -> Staff) (s: Scenario) : Scenario =
        { s with State = GameState.updateStaff (f (rivalCoach s)) s.State }

    let private withMind (f: ManagerMind -> ManagerMind) (c: Staff) = { c with Mind = f c.Mind }

    let private withAdaptability (a: int) (c: Staff) =
        { c with Mental = { c.Mental with Adaptability = a } }

    let private withSkill (ca: int) (pa: int) (c: Staff) =
        { c with CurrentSkill = ca; PotentialSkill = pa }

    let private plan (cfg: RivalManagerConfig) (seed: uint64) (s: Scenario) =
        GamePlanner.prepare cfg seed s.State s.Fixture s.Rival

    let private approachOf (planned: (FootballEngine.Domain.Lineup * GamePlanNote) option) =
        planned |> Option.map (fun (_, note) -> note.Approach)

    let private instructionsOf (planned: (FootballEngine.Domain.Lineup * GamePlanNote) option) : TacticalInstructions =
        match planned with
        | Some(lineup, _) -> lineup.Instructions |> Option.defaultValue TacticalInstructions.defaultInstructions
        | None -> failtest "the coach should have a plan"

    // ---- The user's study ----

    let private centrePattern =
        { PatternVector.empty with
            CorridorLeft = 0.2
            CorridorCenter = 0.6
            CorridorRight = 0.2
            PassShort = 0.3
            PassMedium = 0.5
            PassLong = 0.2 }

    let private recipeInstructions =
        { TacticalInstructions.defaultInstructions with
            Tempo = 4
            Directness = 0 }

    let private record (daysAgo: int) (formation: Formation) (instructions: TacticalInstructions) (pattern: PatternVector) goalsFor goalsAgainst : UserMatchRecord =
        { Date = DateTime(2034, 3, 17).AddDays(float -daysAgo)
          OpponentClubId = 0
          Formation = formation
          Instructions = instructions
          Pattern = pattern
          GoalsFor = goalsFor
          GoalsAgainst = goalsAgainst }

    /// `n` straight wins with one recipe, newest first.
    let private winning (n: int) : UserMatchRecord list =
        [ for k in 1..n -> record (k * 7) F433 recipeInstructions centrePattern 2 0 ]

    let private withStudy (study: UserMatchRecord list) (s: Scenario) : Scenario =
        { s with State = { s.State with UserStudy = study } }

    let private withRivalRecord (margin: int) (met: DateTime) (s: Scenario) : Scenario =
        let record: RivalRecord =
            { Played = 1
              Won = (if margin < 0 then 1 else 0)
              Drawn = 0
              Lost = (if margin > 0 then 1 else 0)
              GoalsFor = max 0 -margin
              GoalsAgainst = max 0 margin
              LastMargin = margin
              LastMet = met }

        editRival (withMind (fun m -> { m with Rivals = m.Rivals |> Map.add (userCoachId s) record })) s

    // ---- Squads ----

    let private setInts<'T> (value: int) (o: 'T) : 'T =
        FSharpValue.MakeRecord(
            typeof<'T>,
            FSharpValue.GetRecordFields o
            |> Array.map (fun field ->
                match field with
                | :? int -> box value
                | other -> other)
        )
        :?> 'T

    let private levelled (value: int) (p: Player) : Player =
        { p with
            Physical = setInts value p.Physical
            Technical = setInts value p.Technical
            Mental = setInts value p.Mental
            Goalkeeping = setInts value p.Goalkeeping
            Condition = 100
            Status = Available }

    /// The rival's club reduced to twelve interchangeable players, so what the coach picks can only follow tiredness.
    let private twelveFor (s: Scenario) (levels: int list) (condition: int list) : Scenario * Player list =
        let club = s.State.Clubs[s.Rival]
        let ids = club.PlayerIds |> List.truncate 12

        let players =
            List.zip3 ids levels condition
            |> List.map (fun (id, level, cond) -> { levelled level s.State.Players[id] with Condition = cond })

        let state =
            { s.State with
                Clubs = s.State.Clubs |> Map.add s.Rival { club with PlayerIds = ids }
                Players = (s.State.Players, players) ||> List.fold (fun acc p -> acc |> Map.add p.Id p) }

        { s with State = state }, players

    let private inXi (planned: (FootballEngine.Domain.Lineup * GamePlanNote) option) (p: Player) =
        match planned with
        | Some(lineup, _) -> lineup.Slots |> List.exists (fun slot -> slot.PlayerId = Some p.Id)
        | None -> failtest "the coach should have a plan"

    // ---- Generation ----

    let private countryData () = baseState.Value.Countries |> Map.toSeq |> Seq.head |> snd

    let private generatedCoach (seed: uint64) : Staff =
        RNG.withSeed seed (fun () -> StaffGen.create 1 HeadCoach 1 (countryData ()) 0 2034)

    let private neutralCoach (traits: ManagerMind -> ManagerMind) (adaptability: int) (ca: int) : Staff =
        let coach = generatedCoach 5UL

        { coach with
            CurrentSkill = ca
            Mental = { coach.Mental with Adaptability = adaptability }
            Mind = traits ManagerMind.neutral }

    let private generationTests =
        testList
            "generation and archetypes"
            [ testCase "top-flight head coaches cover every archetype and none dominates" (fun _ ->
                  let coaches = [ for i in 1..300 -> generatedCoach (uint64 i * 0x9E3779B97F4A7C15UL + 1UL) ]

                  let share archetype =
                      float (coaches |> List.filter (fun c -> ManagerMind.archetypeOf c = archetype) |> List.length) / 300.0

                  for archetype in [ Copycat; Hothead; Strategist; Romantic; Gambler; Conservative; Pragmatist ] do
                      let s = share archetype
                      Expect.isGreaterThanOrEqual s 0.03 $"{archetype} is represented ({s:P0})"
                      Expect.isLessThanOrEqual s 0.45 $"{archetype} does not dominate ({s:P0})"

                  let copiers = float (coaches |> List.filter (fun c -> c.Mind.Imitation >= 15) |> List.length) / 300.0
                  Expect.isGreaterThanOrEqual copiers 0.08 "about one coach in seven is a copier"
                  Expect.isLessThanOrEqual copiers 0.24 "no more"

                  for c in coaches do
                      for trait' in [ c.Mind.Boldness; c.Mind.Temperament; c.Mind.Pride; c.Mind.Imitation; c.Mental.Adaptability ] do
                          Expect.isTrue (trait' >= 1 && trait' <= 20) "traits stay on the 1..20 scale")

              testCase "the same seed makes the same coach" (fun _ ->
                  Expect.equal (generatedCoach 77UL) (generatedCoach 77UL) "deterministic per seed"
                  Expect.notEqual (generatedCoach 77UL).Mind (generatedCoach 78UL).Mind "and another seed another one")

              testCase "every staff member of a generated club has a personality" (fun _ ->
                  for staff in baseState.Value.Staff |> Map.values do
                      Expect.isTrue
                          (staff.Mind.Boldness >= 1 && staff.Mind.Imitation <= 20 && staff.Mind.Rivals.IsEmpty)
                          "an assistant can be promoted, so he needs one too")

              testCase "archetype rules apply in order" (fun _ ->
                  let arch c = ManagerMind.archetypeOf c

                  Expect.equal (arch (neutralCoach (fun m -> { m with Imitation = 18; Temperament = 18 }) 10 100)) Copycat "imitation first"
                  Expect.equal (arch (neutralCoach (fun m -> { m with Temperament = 16 }) 17 170)) Hothead "then temper, even over adaptability"
                  Expect.equal (arch (neutralCoach id 16 150)) Strategist "adaptable and good"
                  Expect.equal (arch (neutralCoach id 16 120)) Pragmatist "adaptable but not good enough is nothing special"
                  Expect.equal (arch (neutralCoach id 5 100)) Romantic "dogmatic possession coach"
                  Expect.equal (arch (neutralCoach (fun m -> { m with Philosophy = CoachPhilosophy.Counter }) 5 100)) Pragmatist "a dogmatic counter coach is no romantic"
                  Expect.equal (arch (neutralCoach (fun m -> { m with Boldness = 17 }) 10 100)) Gambler "bold"
                  Expect.equal (arch (neutralCoach (fun m -> { m with Boldness = 4 }) 10 100)) Conservative "timid"
                  Expect.equal (arch (neutralCoach (fun m -> { m with Philosophy = CoachPhilosophy.Fortress }) 10 100)) Conservative "fortress"
                  Expect.equal (arch (neutralCoach id 10 100)) Pragmatist "the rest")

              testCase "reading is CA over 200 and learning is PA over 200" (fun _ ->
                  let coach = neutralCoach id 10 120 |> withSkill 120 180
                  Expect.floatClose Accuracy.high (ManagerMind.reading coach) 0.6 "reading"
                  Expect.floatClose Accuracy.high (ManagerMind.learning coach) 0.9 "learning"
                  Expect.equal (ManagerMind.reading { coach with CurrentSkill = 400 }) 1.0 "clamped")

              testCase "the default rival-manager config is valid and a broken one is refused with its name" (fun _ ->
                  Expect.isOk (ConfigValidation.validateAll BalanceConfig.defaultConfig) "defaults validate"

                  let broken (change: RivalManagerConfig -> RivalManagerConfig) =
                      match ConfigValidation.validateAll { BalanceConfig.defaultConfig with RivalManager = change cfg } with
                      | Error errors -> errors
                      | Ok() -> []

                  Expect.contains (broken (fun c -> { c with StudyWindow = 0 })) "RivalManager.StudyWindow must be in [1, 100]" "window"
                  Expect.isTrue
                      (broken (fun c -> { c with PressingPreset = { c.PressingPreset with PreferredFormation = "F999" } })
                       |> List.exists (fun e -> e.Contains "PressingPreset"))
                      "unknown preset formation"

                  Expect.isTrue
                      (broken (fun c -> { c with HotheadMinutesBetweenMoves = c.MinMinutesBetweenMoves + 1 })
                       |> List.exists (fun e -> e.Contains "HotheadMinutesBetweenMoves"))
                      "hotheads cannot wait longer than calm coaches") ]

    // ---- Study and learning ----

    let private studyTests =
        testList
            "study of the user's recipe"
            [ testCase "the dominant recipe is the most used signature of the window, newest first on a tie" (fun _ ->
                  let other = { TacticalInstructions.defaultInstructions with Mentality = 4 }

                  let study =
                      [ record 7 F442 other PatternVector.empty 0 1
                        record 14 F433 recipeInstructions centrePattern 2 0
                        record 21 F433 recipeInstructions centrePattern 1 1
                        record 28 F442 other PatternVector.empty 0 2
                        record 35 F433 recipeInstructions centrePattern 3 0 ]

                  let state = { (scenario ()).State with UserStudy = study }

                  match GamePlanner.dominantRecipe cfg state with
                  | Some recipe ->
                      Expect.equal recipe.Formation F433 "three of five"
                      Expect.equal recipe.Matches 3 "its matches"
                      Expect.floatClose Accuracy.high recipe.Ppg ((3.0 + 1.0 + 3.0) / 3.0) "points per game"
                      Expect.floatClose Accuracy.high recipe.Pattern.CorridorCenter 0.6 "its mean pattern"
                  | None -> failtest "the study has a recipe"

                  let tied = { state with UserStudy = [ record 1 F442 other PatternVector.empty 0 1; record 2 F433 recipeInstructions centrePattern 2 0 ] }
                  Expect.equal (GamePlanner.dominantRecipe cfg tied |> Option.map _.Formation) (Some F442) "a tie goes to the recipe that is still current"

                  Expect.isNone (GamePlanner.dominantRecipe cfg { state with UserStudy = [] }) "no matches, no recipe")

              testCase "matches older than the window are not studied" (fun _ ->
                  let study = (winning 10) @ [ record 400 F442 TacticalInstructions.defaultInstructions PatternVector.empty 0 0; record 401 F442 TacticalInstructions.defaultInstructions PatternVector.empty 0 0 ]
                  let state = { (scenario ()).State with UserStudy = study }
                  Expect.equal (GamePlanner.dominantRecipe cfg state |> Option.map _.Matches) (Some 10) "only the window counts")

              testCase "awareness grows with every match the coach has seen and with his potential" (fun _ ->
                  let coach = neutralCoach id 10 100 |> withSkill 100 150
                  let a1 = GamePlanner.awareness cfg coach 1
                  let a4 = GamePlanner.awareness cfg coach 4
                  Expect.isLessThan a1 a4 "four matches teach more than one"
                  Expect.isGreaterThan a1 0.0 "and one teaches something"
                  Expect.isLessThan
                      (GamePlanner.awareness cfg (coach |> withSkill 100 80) 3)
                      (GamePlanner.awareness cfg (coach |> withSkill 100 190) 3)
                      "a high-potential coach learns faster")

              testCase "a high-PA coach sees a winning recipe as a threat in fewer matches than a low-PA one" (fun _ ->
                  let baseCoach = neutralCoach id 10 100

                  let crossing (pa: int) =
                      [ 1..10 ]
                      |> List.tryFind (fun n ->
                          let state = { (scenario ()).State with UserStudy = winning n }
                          GamePlanner.threat cfg (baseCoach |> withSkill 100 pa) (GamePlanner.dominantRecipe cfg state) >= cfg.CounterThreatMin)

                  match crossing 190, crossing 70 with
                  | Some high, Some low ->
                      Expect.isLessThan high low "the quick learner reacts sooner"
                      Expect.isLessThanOrEqual high 3 "in about two matches"
                      Expect.isGreaterThanOrEqual low 4 "where the slow one needs four or five"
                  | high, low -> failtestf "both should get there: %A %A" high low)

              testCase "a recipe that loses is no threat however well it is known" (fun _ ->
                  let coach = neutralCoach id 10 100 |> withSkill 100 200
                  let losing = [ for k in 1..8 -> record (k * 7) F433 recipeInstructions centrePattern 0 2 ]
                  let state = { (scenario ()).State with UserStudy = losing }
                  Expect.equal (GamePlanner.threat cfg coach (GamePlanner.dominantRecipe cfg state)) 0.0 "no points, no threat"
                  Expect.equal (GamePlanner.threat cfg coach None) 0.0 "no study, no threat") ]

    // ---- The plan ----

    let private approachTests =
        testList
            "approaches"
            [ testCase "a stubborn coach plays his own football against a strong recipe, an adaptive one answers it" (fun _ ->
                  let strong = scenario () |> withStudy (winning 6)

                  let stubborn =
                      strong
                      |> editRival (withMind (fun m -> { m with Imitation = 2; Pride = 3 }) >> withAdaptability 3 >> withSkill 200 200)

                  let adaptive =
                      strong
                      |> editRival (withMind (fun m -> { m with Imitation = 2; Pride = 3 }) >> withAdaptability 20 >> withSkill 200 200)

                  let seeds = [ for i in 1..40 -> uint64 i * 0x9E3779B97F4A7C15UL ]
                  let approaches (s: Scenario) = seeds |> List.map (fun seed -> approachOf (plan noUnderdog seed s))

                  let stubbornIdentity = approaches stubborn |> List.filter ((=) (Some OwnIdentity)) |> List.length
                  Expect.isGreaterThanOrEqual stubbornIdentity 24 "a coach with adaptability 3 mostly ignores the recipe"

                  Expect.isTrue
                      (approaches adaptive |> List.forall ((=) (Some(CounterRecipe F433))))
                      "an adaptive coach answers the 4-3-3 every time")

              testCase "an adaptive coach narrows his width against a centre-heavy side" (fun _ ->
                  let prepared =
                      scenario ()
                      |> editRival (withMind (fun m -> { m with Philosophy = CoachPhilosophy.Pressing; Imitation = 2; Pride = 3 }) >> withAdaptability 20 >> withSkill 200 200)

                  let own = instructionsOf (plan noUnderdog 9UL prepared)
                  let answer = instructionsOf (plan noUnderdog 9UL (prepared |> withStudy (winning 6)))
                  Expect.isLessThan answer.Width own.Width "he closes the middle"

                  let wide = { centrePattern with CorridorCenter = 0.1; CorridorLeft = 0.45; CorridorRight = 0.45 }

                  let againstWide =
                      instructionsOf (plan noUnderdog 9UL (prepared |> withStudy [ for k in 1..6 -> record (k * 7) F433 recipeInstructions wide 2 0 ]))

                  Expect.isGreaterThan againstWide.Width own.Width "and opens up against a side that attacks down the flanks")

              testCase "a copycat who lost to a formation plays it when his squad suits it, and not when it does not" (fun _ ->
                  let copycat =
                      scenario ()
                      |> editRival (withMind (fun m -> { m with Imitation = 20; Pride = 1 }) >> withAdaptability 1 >> withSkill 200 200)

                  let met = DateTime(2034, 2, 3)
                  let recipe = { TacticalInstructions.defaultInstructions with Tempo = 4; Directness = 0 }
                  let players = GameState.getSquad copycat.Rival copycat.State |> List.filter (fun p -> p.Status = Available)
                  let suit f = GamePlanner.squadSuitability players f
                  let best = FormationLineups.all |> List.map suit |> List.max
                  let worst = FormationLineups.all |> List.minBy suit
                  let ratio = suit worst / best
                  Expect.isLessThan ratio 1.0 "the squad does not suit every shape equally"

                  let lost =
                      copycat
                      |> withRivalRecord (-2) met
                      |> withStudy [ ({ record 40 worst recipe PatternVector.empty 2 0 with Date = met }) ]

                  let suited = plan { noUnderdog with CopySuitabilityMin = ratio - 0.001 } 3UL lost
                  Expect.equal (approachOf suited) (Some(CopyRecipe worst)) "he copies what beat him"
                  Expect.equal (suited |> Option.map (fun (l, _) -> l.Formation)) (Some worst) "the formation"
                  Expect.equal (instructionsOf suited) recipe "and the instructions"

                  let unsuited = plan { noUnderdog with CopySuitabilityMin = ratio + 0.001 } 3UL lost
                  Expect.notEqual (approachOf unsuited) (Some(CopyRecipe worst)) "he does not copy a shape his squad cannot play"

                  let won = copycat |> withRivalRecord 2 met |> withStudy [ ({ record 40 worst recipe PatternVector.empty 0 2 with Date = met }) ]
                  Expect.notEqual (approachOf (plan { noUnderdog with CopySuitabilityMin = 0.0 } 3UL won)) (Some(CopyRecipe worst)) "nor does he copy a side he beat")

              testCase "a proud coach beaten 0-4 plays revenge, more aggressively than his usual plan" (fun _ ->
                  let proud =
                      scenario ()
                      |> editRival (withMind (fun m -> { m with Philosophy = CoachPhilosophy.Fortress; Pride = 20; Imitation = 2 }) >> withAdaptability 1 >> withSkill 200 200)

                  let met = DateTime(2034, 2, 3)
                  let usual = plan noUnderdog 5UL proud
                  let revenge = plan noUnderdog 5UL (proud |> withRivalRecord (-4) met)

                  Expect.equal (approachOf usual) (Some OwnIdentity) "without the humiliation he plays his own football"
                  Expect.equal (approachOf revenge) (Some Revenge) "after it he wants the rematch"
                  Expect.isGreaterThan (instructionsOf revenge).Mentality (instructionsOf usual).Mentality "braver"
                  Expect.isGreaterThan (instructionsOf revenge).PressingIntensity (instructionsOf usual).PressingIntensity "and higher up"

                  let calm = proud |> editRival (withMind (fun m -> { m with Pride = 4 })) |> withRivalRecord (-4) met
                  Expect.equal (approachOf (plan noUnderdog 5UL calm)) (Some OwnIdentity) "a humble coach shrugs it off"

                  let narrow = proud |> withRivalRecord (-2) met
                  Expect.equal (approachOf (plan noUnderdog 5UL narrow)) (Some OwnIdentity) "and a narrow defeat is no insult")

              testCase "against a much stronger side a bold underdog presses and a cautious one sits back" (fun _ ->
                  let weak = scenario () |> editRival (withMind (fun m -> { m with Philosophy = CoachPhilosophy.Possession; Imitation = 2; Pride = 3 }) >> withAdaptability 1 >> withSkill 200 200)

                  // Flat squads, so the true gap is exactly eighty CA points.
                  let flatten (club: ClubId) (ca: int) (state: GameState) =
                      (state, GameState.getSquad club state)
                      ||> List.fold (fun gs p -> GameState.updatePlayer { p with CurrentSkill = ca } gs)

                  let strongUser = weak.State |> flatten weak.State.UserClubId 180 |> flatten weak.Rival 100

                  let weak = { weak with State = strongUser }
                  let bold = weak |> editRival (withMind (fun m -> { m with Boldness = 20 }))
                  let cautious = weak |> editRival (withMind (fun m -> { m with Boldness = 2 }))

                  Expect.equal (approachOf (plan cfg 1UL bold)) (Some Underdog) "the gap is seen"
                  Expect.equal (approachOf (plan cfg 1UL cautious)) (Some Underdog) "by both"

                  let boldI = instructionsOf (plan cfg 1UL bold)
                  let cautiousI = instructionsOf (plan cfg 1UL cautious)
                  Expect.isGreaterThan boldI.PressingIntensity cautiousI.PressingIntensity "the bold one presses"
                  Expect.isGreaterThan boldI.Mentality cautiousI.Mentality "and attacks"
                  Expect.isGreaterThan boldI.DefensiveLine cautiousI.DefensiveLine "from further up"

                  // A coach who reads poorly cannot tell: the same gap is seen by far fewer of his seeds.
                  let blind = weak |> editRival (withMind (fun m -> { m with Imitation = 2; Pride = 3 }) >> withSkill 50 50)
                  let seen (s: Scenario) = [ for i in 1..60 -> uint64 i * 0x9E3779B97F4A7C15UL ] |> List.filter (fun seed -> approachOf (plan { cfg with UnderdogGap = 75.0 } seed s) = Some Underdog) |> List.length
                  Expect.isGreaterThan (seen weak) (seen blind) "good readers see the gap more reliably")

              testCase "with nothing to answer a coach plays his philosophy" (fun _ ->
                  let own =
                      scenario ()
                      |> editRival (withMind (fun m -> { m with Philosophy = CoachPhilosophy.Counter; Imitation = 2; Pride = 3 }) >> withAdaptability 1 >> withSkill 120 120)

                  let planned = plan noUnderdog 4UL own
                  Expect.equal (approachOf planned) (Some OwnIdentity) "identity"
                  Expect.equal (instructionsOf planned) cfg.CounterPreset.Values "the preset of his philosophy") ]

    // ---- Invariants ----

    let private plannerTests =
        testList
            "game planner"
            [ testCase "the planner writes only the rival's lineup and plan and never touches the managed club" (fun _ ->
                  let s = scenario () |> withStudy (winning 4)
                  let planned = GamePlanner.prepareForFixtures cfg [ s.FixtureId, s.Fixture ] s.State

                  let userCoach = (GameState.headCoach s.State.UserClubId s.State).Value
                  Expect.equal (GameState.headCoach s.State.UserClubId planned) (Some userCoach) "the user's coach is exactly as he was"

                  let rivalId = rivalCoach s |> _.Id
                  let others = s.State.Staff |> Map.filter (fun id _ -> id <> rivalId)

                  Expect.equal (planned.Staff |> Map.filter (fun id _ -> others.ContainsKey id)) others "nobody else changed"

                  let coach = (GameState.headCoach s.Rival planned).Value
                  Expect.isSome coach.Attributes.Coaching.Lineup "the rival has his XI"
                  Expect.equal (coach.Mind.LastPlan |> Option.map _.OpponentClubId) (Some s.State.UserClubId) "and remembers whom the plan was for"
                  Expect.equal (coach.Mind.LastPlan |> Option.map _.Date) (Some s.Fixture.ScheduledDate) "and when")

              testCase "fixtures without the user, and a user without a club, plan nothing" (fun _ ->
                  let s = scenario ()

                  let foreign =
                      s.State.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq)
                      |> Seq.find (fun (_, f) -> MatchFixture.isPending f && not (MatchFixture.involves s.State.UserClubId f))

                  Expect.isTrue (obj.ReferenceEquals(GamePlanner.prepareForFixtures cfg [ foreign ] s.State, s.State)) "an AI fixture is the background model's business"

                  let dismissed =
                      GameState.updateStaff { (GameState.userManager s.State).Value with Status = Unemployed } s.State

                  Expect.isTrue (obj.ReferenceEquals(GamePlanner.prepareForFixtures cfg [ s.FixtureId, s.Fixture ] dismissed, dismissed)) "with no managed club there is nobody to study"

                  Expect.isNone (GamePlanner.prepare cfg 1UL s.State s.Fixture s.State.UserClubId) "the user's own club never gets a plan")

              testCase "the preview is the plan the match gets" (fun _ ->
                  let s = scenario () |> withStudy (winning 5)
                  let coach = rivalCoach s
                  let direct = GamePlanner.prepare cfg (GamePlanner.seedFor s.State s.Fixture coach.Id) s.State s.Fixture s.Rival
                  Expect.isSome direct "there is a plan"
                  Expect.equal (GamePlanner.preview cfg s.State s.Fixture) direct "preview equals prepare for the same state and seed"
                  Expect.equal (GamePlanner.preview cfg s.State s.Fixture) (GamePlanner.preview cfg s.State s.Fixture) "and asking twice changes nothing"

                  let planned = GamePlanner.prepareForFixtures cfg [ s.FixtureId, s.Fixture ] s.State
                  let written = (GameState.headCoach s.Rival planned).Value
                  Expect.equal written.Attributes.Coaching.Lineup (direct |> Option.map fst) "the lineup written is the one previewed"
                  Expect.equal written.Mind.LastPlan (direct |> Option.map snd) "and so is the note")

              testCase "a high-reading coach benches a tired starter whom a low-reading coach keeps" (fun _ ->
                  let s = scenario ()
                  // Twelve players: eleven at level 14, one at 12; the first of the eleven is at 55% condition.
                  let squad, players =
                      twelveFor s ([ for _ in 1..11 -> 14 ] @ [ 12 ]) (55 :: [ for _ in 1..11 -> 100 ])

                  let tired = players.Head

                  let planAs ca =
                      plan noUnderdog 2UL (squad |> editRival (withSkill ca 200 >> withAdaptability 1 >> withMind (fun m -> { m with Imitation = 2 })))

                  Expect.isFalse (inXi (planAs 190) tired) "the coach who reads the squad rests him"
                  Expect.isTrue (inXi (planAs 50) tired) "the one who does not plays him anyway")

              testCase "an unrested squad is picked on suitability alone, whoever reads it" (fun _ ->
                  let s = scenario ()
                  let squad, players = twelveFor s ([ for _ in 1..11 -> 14 ] @ [ 12 ]) [ for _ in 1..12 -> 100 ]
                  let spare = List.last players

                  for ca in [ 50; 190 ] do
                      Expect.isFalse
                          (inXi (plan noUnderdog 2UL (squad |> editRival (withSkill ca 200))) spare)
                          "the weaker player stays on the bench")

              testCase "contexts name the sides without a person as autonomous, with the coach's own traits" (fun _ ->
                  let s = scenario () |> editRival (withMind (fun m -> { m with Boldness = 18; Temperament = 4 }) >> withAdaptability 15 >> withSkill 150 150)
                  let planned =
                      GamePlanner.prepareForFixtures cfg [ s.FixtureId, s.Fixture ] s.State
                      |> LineupOps.ensureForFixtures [ s.FixtureId, s.Fixture ]

                  match MatchContextBuilder.build planned s.Fixture BalanceConfig.defaultConfig with
                  | None -> failtest "the fixture should line up"
                  | Some ctx ->
                      let mine, theirs =
                          if s.Fixture.HomeClubId = s.State.UserClubId then ctx.HomeClub, ctx.AwayClub else ctx.AwayClub, ctx.HomeClub

                      Expect.isFalse mine.Manager.Autonomous "the user's side is commanded"
                      Expect.isTrue theirs.Manager.Autonomous "the rival's is not"
                      Expect.floatClose Accuracy.high (float theirs.Manager.Reading) 0.75 "reading is CA over 200"
                      Expect.floatClose Accuracy.high (float theirs.Manager.Boldness) 0.9 "boldness over 20"
                      Expect.floatClose Accuracy.high (float theirs.Manager.Temperament) 0.2 "temperament over 20"
                      Expect.floatClose Accuracy.high (float theirs.Manager.Adaptability) 0.75 "adaptability over 20"

                  let dismissed = GameState.updateStaff { (GameState.userManager planned).Value with Status = Unemployed } planned

                  match MatchContextBuilder.build dismissed s.Fixture BalanceConfig.defaultConfig with
                  | Some ctx -> Expect.isTrue (ctx.HomeClub.Manager.Autonomous && ctx.AwayClub.Manager.Autonomous) "with no managed club every side is autonomous"
                  | None -> failtest "the fixture should line up")

              testCase "the counter plan is only as sharp as the coach who draws it" (fun _ ->
                  let counter = BalanceConfig.defaultConfig.Counter
                  let signature = "F433", TacticalInstructions.defaultInstructions
                  let vector = PatternVector.empty |> PatternVector.withSignature "F433" TacticalInstructions.defaultInstructions
                  let mem = ScoutingMemory.update BalanceConfig.defaultConfig.Scout DateTime.UtcNow 7 vector ScoutingMemory.empty

                  let baseTactics: TacticsConfig =
                      { Formation = "F442"
                        DefensiveLine = 50.0f
                        PressIntensity = 0.5f
                        Width = 0.5f
                        Tempo = 0.5f
                        Directness = 0.5f
                        Mentality = 0.5f
                        PressTriggerZone = 0.5f }

                  let at sharpness = MatchContextBuilder.adjustTacticsBy sharpness counter mem 7 signature baseTactics
                  Expect.equal (at 0.0) baseTactics "a coach who cannot bend his plan counters nothing"
                  Expect.equal (at 1.0) (MatchContextBuilder.adjustTactics counter mem 7 signature baseTactics) "full sharpness is the configured counter"
                  let full = baseTactics.DefensiveLine - (at 1.0).DefensiveLine
                  let half = baseTactics.DefensiveLine - (at 0.5).DefensiveLine
                  Expect.isGreaterThan full 0.0f "the counter drops the line"
                  Expect.floatClose Accuracy.high (float half) (float full / 2.0) "half the sharpness, half the drop") ]

    // ---- Memory ----

    let private memoryTests =
        testList
            "rivalries and seasons"
            [ testCase "a result is remembered by both coaches from their own side" (fun _ ->
                  let a = neutralCoach id 10 100
                  let b = { neutralCoach id 10 100 with Id = a.Id + 1 }
                  let date = DateTime(2034, 5, 1)
                  let a1, b1 = ManagerMind.recordResult date a b 3 1

                  let recordA = a1.Mind.Rivals[b.Id]
                  let recordB = b1.Mind.Rivals[a.Id]
                  Expect.equal (recordA.Played, recordA.Won, recordA.Lost, recordA.GoalsFor, recordA.GoalsAgainst, recordA.LastMargin) (1, 1, 0, 3, 1, 2) "the winner's book"
                  Expect.equal (recordB.Played, recordB.Won, recordB.Lost, recordB.GoalsFor, recordB.GoalsAgainst, recordB.LastMargin) (1, 0, 1, 1, 3, -2) "the loser's book"
                  Expect.equal recordA.LastMet date "when"

                  let a2, _ = ManagerMind.recordResult (date.AddDays 30.0) a1 b1 0 0
                  let draw = a2.Mind.Rivals[b.Id]
                  Expect.equal (draw.Played, draw.Won, draw.Drawn, draw.GoalsFor) (2, 1, 1, 3) "records accumulate"
                  Expect.equal draw.LastMargin 0 "and keep the last margin"

                  let same, _ = ManagerMind.recordResult date a a 5 0
                  Expect.equal same a "a coach never meets himself")

              testCase "every fixture between two clubs updates both head coaches, and the record follows a coach to a new club" (fun _ ->
                  let s = scenario ()
                  let fixture = s.Fixture
                  let home = (GameState.headCoach fixture.HomeClubId s.State).Value
                  let away = (GameState.headCoach fixture.AwayClubId s.State).Value
                  let after = GamePlanner.recordMeeting fixture 2 1 s.State

                  Expect.equal (after.Staff[home.Id].Mind.Rivals[away.Id].LastMargin) 1 "the home coach's book"
                  Expect.equal (after.Staff[away.Id].Mind.Rivals[home.Id].LastMargin) -1 "the away coach's book"

                  // The away coach moves to a third club; his old book travels with him.
                  let third = s.State.Clubs |> Map.toSeq |> Seq.map fst |> Seq.find (fun id -> id <> fixture.HomeClubId && id <> fixture.AwayClubId)
                  let displaced = (GameState.headCoach third after).Value

                  let moved =
                      after
                      |> GameState.updateStaff { displaced with Role = AssistantManager }
                      |> GameState.updateStaff { after.Staff[away.Id] with Contract = Some { (after.Staff[away.Id].Contract.Value) with ClubId = third } }

                  let later = { fixture with AwayClubId = third; ScheduledDate = fixture.ScheduledDate.AddDays 100.0 }
                  let again = GamePlanner.recordMeeting later 0 0 moved
                  Expect.equal (again.Staff[away.Id].Mind.Rivals[home.Id].Played) 2 "the same two coaches have now met twice, at two different clubs")

              testCase "rivalries nobody renewed fade" (fun _ ->
                  let now = DateTime(2040, 7, 1)

                  let record met : RivalRecord =
                      { Played = 1; Won = 1; Drawn = 0; Lost = 0; GoalsFor = 1; GoalsAgainst = 0; LastMargin = 1; LastMet = met }

                  let coach =
                      neutralCoach (fun m -> { m with Rivals = Map.ofList [ 11, record (now.AddYears(-6)); 12, record (now.AddYears(-1)) ] }) 10 100

                  Expect.equal (ManagerMind.prune cfg now coach).Mind.Rivals.Keys.Count 1 "one is left"
                  Expect.isTrue ((ManagerMind.prune cfg now coach).Mind.Rivals.ContainsKey 12) "the recent one")

              testCase "a young coach grows toward his potential and never past it, an old one declines to a floor" (fun _ ->
                  let now = DateTime(2034, 7, 1)
                  let young = { neutralCoach id 10 100 with Birthday = DateTime(1999, 1, 1); CurrentSkill = 100; PotentialSkill = 180 }

                  let path = List.scan (fun c _ -> ManagerMind.developSeason cfg now c) young [ 1..40 ]
                  Expect.isGreaterThan path.[1].CurrentSkill 100 "he improves in his first season"
                  Expect.isTrue (path |> List.pairwise |> List.forall (fun (a, b) -> b.CurrentSkill >= a.CurrentSkill)) "and never slips while he is young"
                  Expect.isTrue (path |> List.forall (fun c -> c.CurrentSkill <= 180)) "never above his potential"
                  Expect.equal (List.last path).CurrentSkill 180 "and he gets there"

                  let old = { young with Birthday = DateTime(1970, 1, 1); CurrentSkill = 150; PotentialSkill = 190 }
                  Expect.equal (ManagerMind.developSeason cfg now old).CurrentSkill (150 - cfg.CoachDeclinePerYear) "an old coach loses a little each season"
                  let ancient = List.fold (fun c _ -> ManagerMind.developSeason cfg now c) old [ 1..200 ]
                  Expect.equal ancient.CurrentSkill cfg.CoachSkillFloor "but never below the floor"

                  let capped = { young with CurrentSkill = 180 }
                  Expect.equal (ManagerMind.developSeason cfg now capped).CurrentSkill 180 "a coach at his potential stays there")

              testCase "the season rollover develops every coach once" (fun _ ->
                  let s = scenario ()
                  let coach = rivalCoach s
                  let young = { coach with Birthday = DateTime(s.State.Season - 30, 1, 1); CurrentSkill = 100; PotentialSkill = 180 }
                  let state = GameState.updateStaff young s.State
                  let next = SeasonPhase.runEndOfSeason BalanceConfig.defaultConfig state

                  Expect.isGreaterThan next.Staff[coach.Id].CurrentSkill 100 "he grew"
                  Expect.isLessThanOrEqual next.Staff[coach.Id].CurrentSkill 180 "within his potential"
                  Expect.equal next.Staff[coach.Id].CurrentSkill (ManagerMind.developSeason cfg state.CurrentDate young).CurrentSkill "exactly one season of it")

              testCase "a played background fixture is a meeting between the two coaches" (fun _ ->
                  let s = scenario ()

                  let id, fixture =
                      s.State.Competitions
                      |> Map.toSeq
                      |> Seq.collect (fun (_, c) -> c.Fixtures |> Map.toSeq)
                      |> Seq.find (fun (_, f) -> MatchFixture.isPending f && not (MatchFixture.involves s.State.UserClubId f))

                  let state = { s.State with CurrentDate = fixture.ScheduledDate.Date }
                  let after = MatchPhase.runFixtures [ id, fixture ] state

                  let played =
                      after.Competitions |> Map.values |> Seq.pick (fun c -> c.Fixtures |> Map.tryFind id)

                  let home = (GameState.headCoach fixture.HomeClubId after).Value
                  let away = (GameState.headCoach fixture.AwayClubId after).Value
                  Expect.isTrue played.Played "the fixture was played"
                  Expect.equal home.Mind.Rivals[away.Id].LastMargin (played.HomeScore.Value - played.AwayScore.Value) "home book"
                  Expect.equal away.Mind.Rivals[home.Id].LastMargin (played.AwayScore.Value - played.HomeScore.Value) "away book"
                  Expect.isEmpty after.UserStudy "a background match is not on TV: nothing for rivals to study")

              testCase "the study keeps the newest matches within its window" (fun _ ->
                  let s = scenario ()

                  let final =
                      [ 1..15 ]
                      |> List.fold (fun gs k -> GamePlanner.recordUserMatch cfg (record (-k) F433 recipeInstructions centrePattern k 0) gs) s.State

                  Expect.hasLength final.UserStudy cfg.StudyWindow "capped"
                  Expect.equal final.UserStudy.Head.GoalsFor 15 "newest first"
                  Expect.equal (List.last final.UserStudy).GoalsFor 6 "oldest kept") ]

    // ---- A user match, end to end ----

    let private integrationTests =
        testList
            "a user match through the world"
            [ testCase "the user's match is studied, remembered and planned against, and what the preview showed is what was played" (fun _ ->
                  let s = scenario () |> withStudy (winning 4)
                  let clock = WorldClockOps.init s.State.Season
                  let preview = GamePlanner.preview cfg s.State s.Fixture

                  let job =
                      match WorldRunner.prepareUserMatchJobWithBalance BalanceConfig.defaultConfig clock s.State s.FixtureId with
                      | Ok job -> job
                      | Error e -> failtestf "no job: %s" e

                  let lineup = if s.Fixture.HomeClubId = s.Rival then job.Input.Context.HomeLineup else job.Input.Context.AwayLineup

                  let previewed =
                      preview.Value |> fst |> fun l -> l.Slots |> List.sortBy _.Index |> List.choose _.PlayerId

                  Expect.equal
                      (lineup.Profiles |> Array.truncate 11 |> Array.map (fun p -> MatchContextBuilder.unwrapPid p.Id) |> List.ofArray)
                      previewed
                      "the rival fields the XI the preview showed"

                  match WorldRunner.simulateUserMatchWithBalance BalanceConfig.defaultConfig clock s.State with
                  | Error e -> failtestf "match failed: %s" e
                  | Ok result ->
                      let after = result.DayResult.GameState
                      Expect.hasLength after.UserStudy 5 "the match joined the study"

                      let latest = after.UserStudy.Head
                      let played = result.DayResult.PlayedMatches |> List.head |> snd
                      let isHome = played.HomeClubId = s.State.UserClubId
                      Expect.equal latest.OpponentClubId s.Rival "against the rival"
                      Expect.equal latest.GoalsFor (if isHome then played.HomeScore.Value else played.AwayScore.Value) "with the real score"
                      Expect.equal latest.Date played.ScheduledDate "on the day"

                      let coach = (GameState.headCoach s.Rival after).Value
                      Expect.isTrue (coach.Mind.Rivals.ContainsKey (userCoachId s)) "the rival coach remembers the meeting"
                      Expect.equal coach.Mind.LastPlan (preview |> Option.map snd) "and the plan he took in") ]

    let tests =
        testList
            "FootballEngine.Core.World.RivalManagers"
            [ generationTests; studyTests; approachTests; plannerTests; memoryTests ]

    /// Plays one whole match, so it belongs to the full gate only.
    let integration = integrationTests

    // ---- The background model ----

    let quickMatchTests =
        testList
            "FootballEngine.Core.World.RivalManagersQuickMatch"
            [ testCase "a coach who reads the game better lifts his side in the background model" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()
                  let q = ctx.Config.QuickMatch

                  let before (c: MatchContext) =
                      let gap = QuickMatch.strength c.HomeLineup - QuickMatch.strength c.AwayLineup
                      q.GoalsPerTeam * exp (q.HomeAdvantage + q.StrengthSensitivity * gap), q.GoalsPerTeam * exp (-q.StrengthSensitivity * gap)

                  let coaches home away =
                      { ctx with
                          HomeClub = { ctx.HomeClub with Manager = { ctx.HomeClub.Manager with Reading = home } }
                          AwayClub = { ctx.AwayClub with Manager = { ctx.AwayClub.Manager with Reading = away } } }

                  let homeBase, awayBase = before ctx
                  Expect.equal (QuickMatch.rates q (coaches 0.8f 0.8f)) (homeBase, awayBase) "equal coaches change nothing"
                  Expect.equal (QuickMatch.rates q (coaches 0.3f 0.3f)) (homeBase, awayBase) "whatever their level"

                  let homeBetter = QuickMatch.rates q (coaches 0.95f 0.3f)
                  Expect.isGreaterThan (fst homeBetter) homeBase "the better coach's side scores more"
                  Expect.isLessThan (snd homeBetter) awayBase "and concedes less"

                  let awayBetter = QuickMatch.rates q (coaches 0.3f 0.95f)
                  Expect.isGreaterThan (snd awayBetter) awayBase "away too"
                  Expect.isLessThan (fst awayBetter) homeBase "likewise"

                  Expect.isLessThan (fst homeBetter / homeBase) 1.12 "but the edge is modest, a few percent")

              testCase "identical squads: the better coach wins more of the simulated matches" (fun _ ->
                  let ctx = TestBuilders.formationCtx ()

                  let withReadings home away =
                      { ctx with
                          HomeClub = { ctx.HomeClub with Manager = { ctx.HomeClub.Manager with Reading = home } }
                          AwayClub = { ctx.AwayClub with Manager = { ctx.AwayClub.Manager with Reading = away } } }

                  let goals (c: MatchContext) =
                      [ for k in 1..4000 -> QuickMatch.simulate c (uint64 k * 0x9E3779B97F4A7C15UL) ]
                      |> List.fold (fun (h, a) m -> h + m.HomeScore, a + m.AwayScore) (0, 0)

                  let equalH, equalA = goals (withReadings 0.6f 0.6f)
                  let goodH, goodA = goals (withReadings 0.95f 0.25f)
                  Expect.isGreaterThan (goodH - goodA) (equalH - equalA) "the goal difference moves toward the better coach") ]
