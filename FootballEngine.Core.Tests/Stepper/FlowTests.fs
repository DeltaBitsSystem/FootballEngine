namespace FootballEngine.Core.Tests.Stepper

open Expecto
open FootballEngine.Core.Match
open FootballEngine.Core.Tests.Infrastructure

module FlowTests =

    let private ctx = TestBuilders.defaultCtx ()
    let private clock = SimulationClock.defaultClock

    let flowTests =
        testList
            "FootballEngine.Core.Stepper.FlowTests"
            [ testCase "Live stays Live and emits nothing" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let flow', events = Flow.tick clock ctx.Config world 0<subtick> MatchFlow.Live

                  Expect.equal flow' MatchFlow.Live "still live"
                  Expect.isEmpty events "no events")

              testCase "GoalPause expiry awards kickoff to the conceding side" (fun _ ->
                  let world = TestBuilders.minimalWorld ()
                  let scorer = ctx.HomeLineup.Profiles.[9].Id

                  let flow', events =
                      Flow.tick
                          clock
                          ctx.Config
                          world
                          0<subtick>
                          (MatchFlow.GoalPause(1<tickDelta>, ClubSide.Home, Some scorer))

                  match flow' with
                  | MatchFlow.GoalPause(0<tickDelta>, ClubSide.Home, Some s) -> Expect.equal s scorer "scorer kept"
                  | other -> failtestf "expected GoalPause(0), got %A" other

                  match events with
                  | [ { Data = EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Away) } ] -> ()
                  | other -> failtestf "expected kickoff for Away, got %A" other)

              testCase "HalfTime expiry kicks off the second half for Away" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let flow', events =
                      Flow.tick clock ctx.Config world 0<subtick> (MatchFlow.HalfTime(1<tickDelta>))

                  Expect.equal flow' (MatchFlow.HalfTime 0<tickDelta>) "half time expired"

                  match events with
                  | [ { Data = EventData.SetPiece(SetPieceKind.KickOff, ClubSide.Away) } ] -> ()
                  | other -> failtestf "expected kickoff Away, got %A" other)

              testCase "FullTimeReview expiry ends the match" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let flow', events =
                      Flow.tick clock ctx.Config world 0<subtick> (MatchFlow.FullTimeReview(1<tickDelta>))

                  Expect.equal flow' MatchFlow.MatchEnded "match ended"
                  Expect.isEmpty events "no events")

              testCase "InjuryPause expiry restarts with a free kick" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let flow', events =
                      Flow.tick clock ctx.Config world 0<subtick> (MatchFlow.InjuryPause(1<tickDelta>))

                  Expect.equal flow' (MatchFlow.InjuryPause 0<tickDelta>) "injury pause expired"

                  match events with
                  | [ { Data = EventData.SetPiece(SetPieceKind.FreeKick, ClubSide.Home) } ] -> ()
                  | other -> failtestf "expected free kick Home, got %A" other)

              testCase "VARReview counts down without emitting" (fun _ ->
                  let world = TestBuilders.minimalWorld ()

                  let flow', events =
                      Flow.tick
                          clock
                          ctx.Config
                          world
                          0<subtick>
                          (MatchFlow.VARReview(1<tickDelta>, VARIncident.Offside))

                  match flow' with
                  | MatchFlow.VARReview(0<tickDelta>, VARIncident.Offside) -> ()
                  | other -> failtestf "expected VARReview(0), got %A" other

                  Expect.isEmpty events "no events") ]
