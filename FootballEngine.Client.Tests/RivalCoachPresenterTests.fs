module FootballEngine.Client.Tests.RivalCoachPresenterTests

open System
open Expecto
open FootballEngine
open FootballEngine.Core.Match
open FootballEngine.Domain
open FootballEngine.World.Agents

let private cfg = (WeightMergeSettings.mergeWithOverridesSave None).RivalManager

let private world () =
    LiveMatchTests.scheduledCareer () |> snd

let private rivalClub (gs: GameState) =
    match GameState.getUserNextFixture gs with
    | Some(_, fixture) ->
        if fixture.HomeClubId = gs.UserClubId then
            fixture.AwayClubId
        else
            fixture.HomeClubId
    | None -> failtest "the career has no next fixture"

let private withCoachMind (gs: GameState) (change: ManagerMind -> ManagerMind) =
    let coach = (GameState.headCoach (rivalClub gs) gs).Value
    let edited = { coach with Mind = change coach.Mind }
    GameState.updateStaff edited gs, edited

let private project es gs =
    RivalCoachPresenter.project es cfg gs
    |> Option.defaultWith (fun () -> failtest "the card was hidden")

let private note approach : GamePlanNote =
    { OpponentClubId = 0
      Date = DateTime(2026, 8, 1)
      Formation = F433
      Approach = approach
      Threat = 0.5 }

let tests =
    testList
        "RivalCoachPresenter"
        [ testCase "the card names the coach, his archetype and the shape of the plan the planner previews"
          <| fun _ ->
              let gs, coach = withCoachMind (world ()) (fun mind -> { mind with Imitation = 17 })

              let vm = project true gs
              Expect.equal vm.CoachName coach.Name "the rival's head coach"
              Expect.equal vm.Archetype "Copión" "imitation 17 is a copycat"
              Expect.equal vm.Behaviour "Si le ganás con algo, te lo copia." "what a copycat does"

              let _, previewed =
                  (GamePlanner.preview cfg gs (GameState.getUserNextFixture gs |> Option.get |> snd)).Value

              Expect.equal
                  vm.Formation
                  (RivalCoachPresenter.formationLabel previewed.Formation)
                  "the card shows the previewed plan"

              Expect.isTrue (vm.Stars >= 1 && vm.Stars <= 5) "stars stay within 1..5"

          testCase "the same coach reads in English when the locale is English"
          <| fun _ ->
              let gs, _ = withCoachMind (world ()) (fun mind -> { mind with Imitation = 17 })
              let vm = project false gs
              Expect.equal vm.Archetype "Copycat" "English label"
              Expect.equal vm.Behaviour "Beat him with something and he copies it." "English behaviour"

          testCase "every archetype has a distinct label and description in both languages"
          <| fun _ ->
              let all =
                  [ Copycat; Hothead; Strategist; Romantic; Gambler; Conservative; Pragmatist ]

              for es in [ true; false ] do
                  let texts = all |> List.map (RivalCoachPresenter.archetypeText es)
                  Expect.equal (texts |> List.map fst |> List.distinct |> List.length) 7 "seven labels"
                  Expect.equal (texts |> List.map snd |> List.distinct |> List.length) 7 "seven descriptions"

          testCase "stars follow the reading: CA 200 shows five, CA 0 shows one"
          <| fun _ ->
              let gs = world ()
              let coach = (GameState.headCoach (rivalClub gs) gs).Value
              Expect.equal (RivalCoachPresenter.stars { coach with CurrentSkill = 200 }) 5 "a master"
              Expect.equal (RivalCoachPresenter.stars { coach with CurrentSkill = 0 }) 1 "a novice still shows a star"
              Expect.equal (RivalCoachPresenter.stars { coach with CurrentSkill = 100 }) 3 "half the ceiling"

          testCase "without a recorded meeting the card says first meeting"
          <| fun _ ->
              let vm = project true (world ())
              Expect.equal vm.RecordVs RivalCoachPresenter.FirstMeeting "no history yet"

          testCase "the record is told from the user's side: the coach's losses are the user's wins"
          <| fun _ ->
              let gs0 = world ()
              let userCoach = (GameState.headCoach gs0.UserClubId gs0).Value

              let record: RivalRecord =
                  { Played = 5
                    Won = 1
                    Drawn = 1
                    Lost = 3
                    GoalsFor = 4
                    GoalsAgainst = 9
                    LastMargin = -2
                    LastMet = DateTime(2026, 5, 1) }

              let gs, _ =
                  withCoachMind gs0 (fun mind ->
                      { mind with
                          Rivals = Map.ofList [ userCoach.Id, record ] })

              let vm = project true gs

              Expect.equal
                  vm.RecordVs
                  (RivalCoachPresenter.Meetings(5, 3, 1, 1))
                  "he lost three of five, so the user won three"

          testCase "each approach gives its own reason in Spanish"
          <| fun _ ->
              let gs = world ()
              let coach = (GameState.headCoach (rivalClub gs) gs).Value
              let user = GameState.headCoach gs.UserClubId gs

              let coach =
                  { coach with
                      Mind =
                          { coach.Mind with
                              Philosophy = CoachPhilosophy.Pressing
                              Boldness = 5 } }

              let reason approach =
                  RivalCoachPresenter.planReason true cfg coach user (note approach)

              Expect.equal (reason OwnIdentity) "Juega a lo suyo: presión alta" "identity names the philosophy"

              Expect.equal
                  (reason (CounterRecipe F433))
                  "Te estudió: va a neutralizar tu 4-3-3"
                  "counter names the recipe"

              Expect.equal
                  (reason (CopyRecipe F4231))
                  "Te copia el 4-2-3-1 con el que le ganaste"
                  "copy names the shape"

              Expect.stringContains (reason Underdog) "se cierra" "a cautious underdog sits back"

              let bold =
                  { coach with
                      Mind = { coach.Mind with Boldness = 19 } }

              Expect.stringContains
                  (RivalCoachPresenter.planReason true cfg bold user (note Underdog))
                  "presionarte"
                  "a bold underdog presses"

          testCase "revenge quotes the margin of the last meeting, and the English text differs"
          <| fun _ ->
              let gs = world ()
              let coach0 = (GameState.headCoach (rivalClub gs) gs).Value
              let user = GameState.headCoach gs.UserClubId gs
              let user' = user.Value

              let coach =
                  { coach0 with
                      Mind =
                          { coach0.Mind with
                              Rivals =
                                  Map.ofList
                                      [ user'.Id,
                                        { Played = 1
                                          Won = 0
                                          Drawn = 0
                                          Lost = 1
                                          GoalsFor = 0
                                          GoalsAgainst = 4
                                          LastMargin = -4
                                          LastMet = DateTime(2026, 5, 1) } ] } }

              Expect.equal
                  (RivalCoachPresenter.planReason true cfg coach user (note Revenge))
                  "Busca revancha: la última vez perdió por 4"
                  "Spanish"

              Expect.equal
                  (RivalCoachPresenter.planReason false cfg coach user (note Revenge))
                  "Out for revenge: he lost by 4 last time"
                  "English"

          testCase "the card is hidden without a next fixture"
          <| fun _ ->
              let gs = world ()
              let finished = { gs with Competitions = Map.empty }
              Expect.isNone (RivalCoachPresenter.project true cfg finished) "no fixture, no card"

          testCase "the card is hidden when the rival has no head coach"
          <| fun _ ->
              let gs = world ()
              let rival = rivalClub gs

              let withoutCoach =
                  GameState.getStaff rival gs
                  |> List.filter (fun s -> s.Role = HeadCoach)
                  |> List.fold
                      (fun (state: GameState) coach ->
                          { state with
                              Staff = Map.remove coach.Id state.Staff })
                      gs

              Expect.isNone (GameState.headCoach rival withoutCoach) "precondition: the coach is gone"
              Expect.isNone (RivalCoachPresenter.project true cfg withoutCoach) "no coach, no card" ]
